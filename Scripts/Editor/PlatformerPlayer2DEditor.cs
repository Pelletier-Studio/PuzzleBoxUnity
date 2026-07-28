using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using Codice.Client.BaseCommands;

namespace PuzzleBox
{
    [CustomEditor(typeof(PlatformerPlayer2D))]
    public class PlatformerPlayer2DEditor : Editor
    {
        PlatformerPlayer2D player;

        Dictionary<string, bool> groupVisibility = new Dictionary<string, bool>();

        GUIStyle tutorialStyle;
        GUIStyle importantNoteStyle;
        Color separatorColor;
        Color tutorialTextColor;

        void OnEnable()
        {
            player = (PlatformerPlayer2D)target;
        }

        void InitStyles()
        {
            if (tutorialStyle != null) return;

            separatorColor = new Color(1, 1, 1, 0.3f);
            tutorialTextColor = Color.white;

            tutorialStyle = new GUIStyle(EditorStyles.label);
            tutorialStyle.normal.textColor = tutorialTextColor;
            tutorialStyle.wordWrap = true;

            importantNoteStyle = new GUIStyle(EditorStyles.label);
            importantNoteStyle.normal.textColor = tutorialTextColor;
            importantNoteStyle.wordWrap = true;
            importantNoteStyle.fontStyle = FontStyle.Bold;
        }

        
        private void ShowGroup(string label, Action drawParams) {
            bool visible = groupVisibility.GetValueOrDefault(label, false);
            visible = EditorGUILayout.BeginFoldoutHeaderGroup(visible, label);
            groupVisibility[label] = visible;
            if (visible) {
                drawParams?.Invoke();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void ShowMultilineLabel(string text, GUIStyle style) {
            EditorGUILayout.LabelField(text, style);
            EditorGUILayout.Space();
        }

        private void ShowTutorialText(string text) {
            ShowMultilineLabel(text, tutorialStyle);
        }

        private void ShowImportantNote(string text) {
            ShowMultilineLabel(text, importantNoteStyle);
        }

        private void ShowSeparator() {
            EditorGUILayout.Space();
            float height = 1f; // Pixels
            Rect rect = EditorGUILayout.GetControlRect(false, height);
            EditorGUI.DrawRect(rect, separatorColor);
            EditorGUILayout.Space();
        }

        public override void OnInspectorGUI()
        {
            InitStyles();
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            ShowGroup("物理演算", () => {
                ShowTutorialText("ここで物理演算を行うかどうかを指定することができる。行わないようにするとプレーヤーキャラクターが動作しなくなるので、普段は有効にする。");
                player.simulatePhysics = EditorGUILayout.Toggle("物理演算を行う", player.simulatePhysics);
                EditorGUILayout.Space();
                ShowTutorialText("プレーヤーの質量が高くなると、質量の高いリジッドボディが押せるようになる。逆に質量が少ないと押す力が弱くなる。");
                player.mass = EditorGUILayout.FloatField("質量", player.mass);

                ShowSeparator();
                ShowTutorialText("重力の影響を受けないようにすると、落下しなくなる。");
                player.useGravity = EditorGUILayout.Toggle("重力の影響を受ける", player.useGravity);

                EditorGUILayout.Space();
                ShowTutorialText("こちらのパラメータを変えることによって、他のオブジェクトに比べてキャラクターが受ける重力の力を変えることができる。標準は「1」。マイナス値にすると落下ではなく、浮上する。");
                player.gravityModifier = EditorGUILayout.FloatField("重力の調整", player.gravityModifier);
                
                ShowSeparator();
            });

            ShowGroup("衝突判定", () => {
                ShowTutorialText("以下の2つの角度（0°～90°）を使って、壁を地面と天井と識別する。");
                
                player.maxGroundAngleDegrees = EditorGUILayout.Slider("地面の最大傾斜角", player.maxGroundAngleDegrees, 0, 90);
                player.maxCeilingAngleDegrees = EditorGUILayout.Slider("天井の最大角度", player.maxCeilingAngleDegrees, 0, 90);

                
                ShowSeparator();
                ShowTutorialText("リジッドボディが当たった時の挙動をここで指定する。押し出しを不可にすると、他のオブジェクトに押されても動かない。可にすると、そのオブジェクトの力によって、プレーヤーの入力がなくてもキャラクターが押されて動く。お互いの質量（物理演算設定）によって押される力が変わる。");
                
                player.pushable = EditorGUILayout.Toggle("押し出し可能", player.pushable);

                EditorGUILayout.Space();
                ShowTutorialText("同じ質量のオブジェクトが衝突した時に使われる少し高度な設定。");
                player.pushPriority = EditorGUILayout.IntField("押し出し優先度", player.pushPriority);
                
                ShowSeparator();
                ShowTutorialText("Unity標準機能のレイヤーを使って、衝突するオブジェクトを限定することができる。チェックが外れているレイヤーに入っているオブジェクトと衝突しない。");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("collisionMask"), new GUIContent("衝突レイヤー"));

                ShowSeparator();
            });

            ShowGroup("速度制限", () => {
                ShowTutorialText("こちらのパラメータで各方向ごとに速度制限を設けることができる。単位はユニット/s。これは絶対的な制限で何があっても超えることはない。");
                player.maxSpeedSide = EditorGUILayout.FloatField("横方向の最高速度", player.maxSpeedSide);
                player.maxSpeedUp = EditorGUILayout.FloatField("上方向の最高速度", player.maxSpeedUp);
                player.maxSpeedDown = EditorGUILayout.FloatField("下方向の最高速度", player.maxSpeedDown);

                ShowSeparator();
            });

            ShowGroup("移動", () => {
                ShowTutorialText("移動方向に向かうようにスプライトを反転させるか。");
                player.faceMotionDirection = EditorGUILayout.Toggle("スプライト反転", player.faceMotionDirection);
                ShowSeparator();

                ShowTutorialText("こちらでキャラクターが歩く時の最高速度と加速度を指定する。");
                player.walkSpeed = EditorGUILayout.FloatField("歩行速度", player.walkSpeed);
                player.walkAcceleration = EditorGUILayout.FloatField("歩行加速度", player.walkAcceleration);

                EditorGUILayout.Space();
                ShowTutorialText("こちらでキャラクターが走る時の最高速度と加速度を指定する。");
                player.runSpeed = EditorGUILayout.FloatField("走行速度", player.runSpeed);
                player.runAcceleration = EditorGUILayout.FloatField("走行加速度", player.runAcceleration);

                EditorGUILayout.Space();
                ShowTutorialText("歩く時も走る時も減速度が同じ。この設定を低くすると氷の上で移動するようになる。より正確な操作感を目指すならこの設定と加速度を高めに設定する。");
                player.breakingForce = EditorGUILayout.FloatField("ブレーキ力", player.breakingForce);

                EditorGUILayout.Space();
                ShowTutorialText("ジャンプをした後、落下した後、空中でもプレーヤーが入力すればキャラクターを動かすことができる。空中になった時に空中速度を超えている場合、移動している方向には加速できないが、空中の速度制限に落とされることがない。");
                player.airSpeed = EditorGUILayout.FloatField("空中速度", player.airSpeed);
                player.airAcceleration = EditorGUILayout.FloatField("空中加速度", player.airAcceleration);
                player.airBreakingForce = EditorGUILayout.FloatField("空中ブレーキ力", player.airBreakingForce);

                ShowSeparator();
                ShowTutorialText("これを有効にすると、動いている地面の上に立ったら地面と一緒に動く。");
                ShowImportantNote("立っているオブジェクトはKinematicMotion2Dコンポーネントを通して動かさないとこの機能は無効。");
                player.useGroundMotion = EditorGUILayout.Toggle("地面の動きに追従する", player.useGroundMotion);
                ShowSeparator();
            });

            ShowGroup("ジャンプ", () => {
                ShowTutorialText("ジャンプボタンを押す時間によってジャンプの高さが変わる。瞬間的にボタンを離した場合とずっと押しっぱなしにした場合の高さをここで指定する。");
                player.minJumpHeight = EditorGUILayout.FloatField("最小ジャンプ高さ", player.minJumpHeight);
                player.maxJumpHeight = EditorGUILayout.FloatField("最大ジャンプ高さ", player.maxJumpHeight);

                EditorGUILayout.Space();
                ShowTutorialText("静止した状態でジャンプをすると通常まっすぐ上に移動するが、強制的に斜めにジャンプしたい場合、ここで角度を度で指定する。キャラクターが向いている方向にジャンプする。");
                player.jumpAngle = EditorGUILayout.FloatField("ジャンプ角度", player.jumpAngle);

                EditorGUILayout.Space();
                ShowTutorialText("助走をしてジャンプした場合、移動速度によってジャンプが高くなる設定。");
                player.jumpHeightSpeedBoost = EditorGUILayout.FloatField("助走によるジャンプ補正", player.jumpHeightSpeedBoost);

                EditorGUILayout.Space();
                ShowTutorialText("地面に立っていないとジャンプができない。しかし、連続でジャンプをした時に地面の確認を緩めることができる。そうすることによってより爽快な操作感が得られる場合がある。");
                player.jumpGroundCheckDistance = EditorGUILayout.FloatField("地面チェック距離", player.jumpGroundCheckDistance);

                EditorGUILayout.Space();
                ShowTutorialText("空中でジャンプボタンが押された後に着地した場合、以下の時間（秒）が経過していないとジャンプが発効する。");
                ShowImportantNote("「地面チェック距離」設定と同様の効果が得られるので、片方を必ず「0」にしてください。");
                player.jumpBufferTime = EditorGUILayout.FloatField("ジャンプバッファ時間", player.jumpBufferTime);

                EditorGUILayout.Space();
                ShowTutorialText("空中で上昇する速度が以下の設定より小さくなると重力を一時的に弱くすることができる。そうすることによってキャラクターがジャンプの頂点で一瞬浮くように見える。");
                player.hangSpeed = EditorGUILayout.FloatField("滞空速度", player.hangSpeed);
                player.hangGravityRatio = EditorGUILayout.Slider("滞空時の重力比率", player.hangGravityRatio, 0.1f, 1f);

                ShowSeparator();
                ShowTutorialText("空中でさらにジャンプすることができるかどうかを指定する。空中ジャンプを有効にすると、空中ジャンプ回数で指定した回数だけ、空中でジャンプできるようになる。空中ジャンプの高さは、地面からのジャンプの高さに対して、空中ジャンプ高さの比率で指定する。");
                player.maxAirJumps = EditorGUILayout.IntField("空中ジャンプ回数", player.maxAirJumps);
                player.airJumpHeightRatio = EditorGUILayout.FloatField("空中ジャンプ高さの比率", player.airJumpHeightRatio);

                EditorGUILayout.Space();
                ShowTutorialText("空中ジャンプを利用せずに、落下を開始した直後にジャンプ指示があった場合、落下開始から以下の時間（秒）が経過していないとジャンプを地面にまだたっているかのように許す。これで判定を緩めることによって、より爽快な操作感が得られる場合がある。");
                player.fallJumpTimeLimit = EditorGUILayout.FloatField("落下ジャンプ猶予時間", player.fallJumpTimeLimit);

                ShowSeparator();
                ShowTutorialText("壁に触れているときにジャンプすることができるかどうかを指定する。壁ジャンプの高さは、地面からのジャンプの高さに対して、壁ジャンプ高さの比率で指定する。壁ジャンプの横方向の速度は、キャラクターが壁から離れるときの横方向の速度。");
                player.canWallJump = EditorGUILayout.Toggle("壁ジャンプ可能", player.canWallJump);
                player.wallJumpHeightRatio = EditorGUILayout.FloatField("壁ジャンプ高さの比率", player.wallJumpHeightRatio);
                player.wallJumpHorizontalVelocity = EditorGUILayout.FloatField("壁ジャンプの横方向速度", player.wallJumpHorizontalVelocity);

                ShowSeparator();
                ShowTutorialText("壁を掴んでいるときにジャンプすることができるかどうかを指定する。壁掴みジャンプの高さは、地面からのジャンプの高さに対して、壁掴みジャンプ高さの比率で指定する。壁掴みジャンプの横方向の速度は、キャラクターが壁から離れるときの横方向の速度。");
                player.canJumpWhenGrabbing = EditorGUILayout.Toggle("壁掴み中にジャンプ可能", player.canJumpWhenGrabbing);
                player.grabJumpHeightRatio = EditorGUILayout.FloatField("壁掴みジャンプ高さの比率", player.grabJumpHeightRatio);
                player.grabJumpHorizontalVelocity = EditorGUILayout.FloatField("壁掴みジャンプの横方向速度", player.grabJumpHorizontalVelocity);

                ShowSeparator();
                ShowTutorialText("登り中にジャンプすることができるかどうかを指定する。登りジャンプの高さは、地面からのジャンプの高さに対して、登りジャンプ高さの比率で指定する。");
                player.canJumpWhenClimbing = EditorGUILayout.Toggle("登り中にジャンプ可能", player.canJumpWhenClimbing);
                player.climbJumpHeightRatio = EditorGUILayout.FloatField("登りジャンプ高さの比率", player.climbJumpHeightRatio);

                ShowSeparator();
            });

            ShowGroup("壁", () => {
                ShowTutorialText("キャラクターの左右に線を引いて壁があるかどうかを確認する。垂直オフセットを指定することによって、壁が変出される位置を上下に調整できる。壁検知距離は、キャラクターからどれくらいの距離で壁を検知するかを指定する。");
                player.wallCheckVerticalOffset = EditorGUILayout.FloatField("壁検知の垂直オフセット", player.wallCheckVerticalOffset);
                player.wallCheckDistance = EditorGUILayout.FloatField("壁検知距離", player.wallCheckDistance);

                ShowSeparator();
                ShowTutorialText("壁に沿って落下した場合、重力を弱めて、落下速度を制限することができる。");
                player.wallSlideGravityRatio = EditorGUILayout.FloatField("壁スライド時の重力比率", player.wallSlideGravityRatio);
                player.wallSlideMaxSpeed = EditorGUILayout.FloatField("壁スライドの最高速度", player.wallSlideMaxSpeed);

                ShowSeparator();
                ShowTutorialText("プレーヤーの操作によって、壁を掴むことを許すかどうか。壁を掴むと、壁に沿って上下移動することができる。");
                player.canGrabWall = EditorGUILayout.Toggle("壁掴み可能", player.canGrabWall);
                
                EditorGUILayout.Space();
                ShowTutorialText("壁を掴んでいるときの最大時間を指定する。これを超えると、掴みが強制的に解除される。");
                player.maxWallGrabTime = EditorGUILayout.FloatField("壁掴みの最大時間", player.maxWallGrabTime);
                EditorGUILayout.Space();
                ShowTutorialText("壁を掴んでいるときの移動速度を指定する。");
                player.wallClimbUpSpeed = EditorGUILayout.FloatField("壁登り速度（上）", player.wallClimbUpSpeed);
                player.wallClimbDownSpeed = EditorGUILayout.FloatField("壁登り速度（下）", player.wallClimbDownSpeed);
                EditorGUILayout.Space();
                ShowTutorialText("壁を掴んで、壁の一番上まで登り切った時に、乗り越えられるように自動的に小さくジャンプする。ここで、そのジャンプの高さと横方向の速度を指定する。設定のコツは、壁を乗り越えられるぎろぎろの値にして、プレーヤーが「ジャンプ」として認識しないようにすること。");

                player.climbOverEdgeJumpHeight = EditorGUILayout.FloatField("壁乗り越えジャンプ高さ", player.climbOverEdgeJumpHeight);
                player.climbOverHorizontalVelocity = EditorGUILayout.FloatField("壁乗り越え横方向速度", player.climbOverHorizontalVelocity);

                ShowSeparator();
            });

            ShowGroup("ダッシュ", () => {
                ShowTutorialText("ダッシュを可能にするかどうか。");
                player.canDash = EditorGUILayout.Toggle("ダッシュ可能", player.canDash);

                EditorGUILayout.Space();
                ShowTutorialText("ダッシュの速度を移動方向ごとに指定する。この速度はダッシュ開始時の速度。");
                player.dashSpeedSide = EditorGUILayout.FloatField("横方向ダッシュ速度", player.dashSpeedSide);
                player.dashSpeedUp = EditorGUILayout.FloatField("上方向ダッシュ速度", player.dashSpeedUp);
                player.dashSpeedDown = EditorGUILayout.FloatField("下方向ダッシュ速度", player.dashSpeedDown);

                EditorGUILayout.Space();
                ShowTutorialText("ダッシュの継続時間。");
                player.dashTime = EditorGUILayout.FloatField("ダッシュ時間", player.dashTime);
                
                
                EditorGUILayout.Space();
                ShowTutorialText("ダッシュを開始した後、プレーヤーの入力を無視することができる。");
                player.dashInputFreezeTime = EditorGUILayout.FloatField("入力無視時間", player.dashInputFreezeTime);

                EditorGUILayout.Space();
                ShowTutorialText("ダッシュをしてから、もう一度ダッシュできるようになるまでの時間。");
                player.dashCoolDownTime = EditorGUILayout.FloatField("クールダウン時間", player.dashCoolDownTime);

                EditorGUILayout.Space();
                ShowTutorialText("この設定を有効にすると、ダッシュの方向は上下左右、そして斜め45度の8方向に制限される。無効にすると、ダッシュの方向はプレーヤーの入力方向に完全に従う。（ゲームパッドでアナログスティックを使用すると違いが分かりやすい。）");
                player.limitDashAngle = EditorGUILayout.Toggle("ダッシュ角度を制限", player.limitDashAngle);

                EditorGUILayout.Space();
                ShowTutorialText("ダッシュ中に重力の影響を弱めることができる。「0」に設定すると重力を完全に無効化できる。");
                player.dashGravityRatio = EditorGUILayout.FloatField("ダッシュ時の重力比率", player.dashGravityRatio);

                EditorGUILayout.Space();
            });

            ShowGroup("登り", () => {
                ShowTutorialText("登りを有効にすると、左右だけでなく、上下も自由に移動できる。普段はこの設定を無効にする。スクリプトで有効にして、はしごなどを作ることができる。");
                player.canClimb = EditorGUILayout.Toggle("登り可能", player.canClimb);

                EditorGUILayout.Space();
                ShowTutorialText("登りの速度を上下と横で別々に指定することができる。歩行や走行と同様に加速度とブレーキ力も指定できる。");
                player.climbSpeedUp = EditorGUILayout.FloatField("登り速度（上）", player.climbSpeedUp);
                player.climbSpeedDown = EditorGUILayout.FloatField("登り速度（下）", player.climbSpeedDown);
                player.climbSpeedSide = EditorGUILayout.FloatField("登り速度（横）", player.climbSpeedSide);
                player.climbAcceleration = EditorGUILayout.FloatField("登り加速度", player.climbAcceleration);
                player.climbBreakingForce = EditorGUILayout.FloatField("登りブレーキ力", player.climbBreakingForce);

                EditorGUILayout.Space();
                ShowTutorialText("登っている時に、通常と違う衝突レイヤー設定を使うことができる。");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("climbCollisionMask"), new GUIContent("登り時の衝突レイヤー"));

                ShowSeparator();
            });

            // ShowGroup("演出", () => {
            //     player.deathAnimationTimeoutSeconds = EditorGUILayout.FloatField("死亡アニメーションのタイムアウト（秒）", player.deathAnimationTimeoutSeconds);
            // });

            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(player);
                PrefabUtility.RecordPrefabInstancePropertyModifications(player);
            }

            ShowSeparator();
            EditorGUILayout.LabelField("デバッグ情報", EditorStyles.boldLabel);

            GUI.enabled = false;
            EditorGUILayout.EnumPopup("State", player.state);
            EditorGUILayout.Vector2Field("Velocity", player.velocity);
            EditorGUILayout.Toggle("Accepts input", player.acceptInput);
            EditorGUILayout.Toggle("Is Grounded", player.isGrounded);
            EditorGUILayout.Toggle("Is Jumping", player.isJumping);
            EditorGUILayout.Toggle("Is Running", player.isRunning);
            EditorGUILayout.Toggle("Is Dashing", player.isDashing);
            EditorGUILayout.Toggle("Is Grabbing", player.isGrabbing);
            EditorGUILayout.Toggle("Is Touching Wall", player.isTouchingWall);
            EditorGUILayout.FloatField("Wall Direction", player.wallDirection);
            EditorGUILayout.Vector2Field("Facing Direction", player.facingDirection);
            EditorGUILayout.Vector2Field("Motion Input", player.motionInput);
            GUI.enabled = true;

            Repaint();
        }
    }
}

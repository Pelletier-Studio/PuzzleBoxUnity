/*
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */
 
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PuzzleBox
{
    /**
     * このクラスはキャラクターや動くオブジェクトの移動・衝突判定を手動で制御します。
     *
     * Unityには物理演算を自動的に処理する「Rigidbody2D」というコンポーネントがありますが、
     * プラットフォームゲームのキャラクターのような精密な動きが必要な場合、自動の物理演算では
     * 壁への食い込みや意図しない滑りなどの問題が起きやすいです。
     *
     * このクラスはそういった問題を避けるために、物理演算を自分で制御します。
     * 具体的には、以下の機能を提供します：
     *   ・重力の適用（自由落下・着地）
     *   ・壁・地面・天井との衝突判定および押し返し
     *   ・斜面に沿ったスライド移動
     *   ・動く地面（乗り物・エレベーターなど）への追従
     *   ・他のキネマティックオブジェクトへの押し出し
     *
     * このクラスを使うには、同じゲームオブジェクトに Rigidbody2D コンポーネントが必要です。
     * Rigidbody2D の種類は自動的に「Kinematic（キネマティック）」に設定されます。
     * キネマティックとは、「物理エンジンの自動計算に任せず、スクリプトで直接動かす」という意味です。
     */
    [RequireComponent(typeof(Rigidbody2D))]
    public class KinematicMotion2D : MonoBehaviour
    {
        public bool simulatePhysics = true;
        public float mass = 1f;

        [Header("重力")] // インスペクターに見出しを表紙する
        public bool useGravity = true; // 重力の影響を受けるか？

        
        public float gravityModifier = 1f; // 重力の力の調整

        [HideInInspector]
        public float gravityMultiplier = 1f; 


        [Header("衝突判定")]
        
        // 地面の最大の傾斜。これ以上急な坂は壁として認識します。
        [Min(0)]
        public float maxGroundAngleDegrees = 45;

        // 天井の最大の角度。地面と同様に壁との識別に使います。
        [Min(0)]
        public float maxCeilingAngleDegrees = 45;

        // 一部のオブジェクトと衝突したくない（すり抜けたい）場合、
        // ここで指定します。
        public LayerMask collisionMask = ~0; // 「~0」はここで「全て」に解釈されます。

        // Rigidbody2Dを使ってプレーヤーキャラクターなどを実装すると、
        // キャラクターがコライダに引っかかったり、壁などに食い込んだりする
        // 不具合がよく発生してしまいます。一つの対策として、衝突・接触する
        // コライダと少しだけ隙間を開けます。「margin」でその隙間の大きさを
        // 調整します。

        [Min(0.005f)]
        public float margin = 0f;

        // オブジェクトが地面に立っているかどうかを判定するために、
        // 以下のパラメータで指定する距離まで、下に地面に該当する
        // コライダがないかを確認します。

        [Min(0.001f)]
        protected float groundCheckDistance = 0.01f;

        public bool pushable = false;

        // 二つの「押せない（pushable=false）」オブジェクト同士、または二つの「押せる（pushable=true）」
        // オブジェクト同士が反対方向へ向かいながら衝突した場合、この値でどうなるかを決めます。
        // pushPriorityが低い方は、pushable=trueなら押しのけられます。
        // pushPriorityが高い方は常に目的地まで移動を続けます。
        // 両方のpushPriorityが同じ場合は、どちらも接触点で止まります。
        public int pushPriority = 0;


        [Min(0f)]
        public float minSlideDistance = 0f;

        // このパラメータは少し上級者向けで従来なら変える必要がありません。
        // 何かと衝突したら、移動方向を変えて移動を続けてみます。
        // （例えば、斜面に衝突したら、止まるのではなく、斜面の上に移動を続けます。）
        // この値はこの処理を繰り返す最大の回数です。
        int maxIterations = 1;


        [Header("速度")]
        // 絶対に超えられない速度。

        [Min(0)]
        public float maxSpeedUp = 100f;

        [Min(0)]
        public float maxSpeedDown = 100f;

        [Min(0)]
        public float maxSpeedSide = 100f;

        [Space]
        [Tooltip("地面が動いている場合、その影響を受けるか？")]
        public bool useGroundMotion = true; // 移動する地面に影響を受けるか

        // 上の「useGroundMotion」は「自分が動く地面に追従するか」という設定ですが、
        // こちらは逆に「自分が地面になった時の性質」を決める設定です。
        //
        // 自由落下より速く下降する地面の上に物体が乗っている場合、どうするべきでしょうか。
        // 現実では、物体は地面に置いていかれて、そのまま落下します（地面が先に下がっていく）。
        // 「sticky」がtrueなら、そうならずに、乗っている物体を必ず引き連れます。
        // エレベーターのように、乗っている物体を絶対に離したくない場合に使います。
        [Tooltip("この地面に乗っている物体を、自由落下より速く下降しても引き連れるか？")]
        public bool sticky = true;

        // [HideInInspector] // インスペクターで隠す。
        public Vector2 velocity; // 移動の速度。基本的に他のコンポーネントがコードで変えます。

        [HideInInspector] // インスペクターで隠す。
        public Vector2 lastGroundVelocity; // 地面に最後に接触していた時の速度。

        // オブジェクトが地面に立っているかどうか。
        // この値はC#の便利な機能「アクセサ」を使って、クラスの外部から変えられないようにしています。
        public bool isGrounded { get; private set; }

        public bool justLanded { get; private set; }
        public bool justFell { get; private set; }

        // 地面を離れてから経過した時間（秒）。ジャンプの判定で使う事があります。
        public float timeInAir { get; private set; }


        // KinematicMotion2Dを別のKinematicMotion2Dに「アタッチ」することができます。
        // アタッチとは、別の紐づけて、アタッチされたオブジェクトは完全に「親」となるオブジェクトの動きに追従します。
        // 例えば、キャラクターがオブジェクトを持ち上げたりする時に使います。
        // オブジェクトがアタッチされている時に、親が動くと、アタッチされた
        private KinematicMotion2D parent = null;
        private List<KinematicMotion2D> attachedMotions = new List<KinematicMotion2D>();

        public void AttachTo(KinematicMotion2D parentMotion)
        {
            if (parentMotion == null)
            {
                Detach();
                return;
            }

            if (parentMotion != parent)
            {
                Detach();
                parent = parentMotion;
                parent.attachedMotions.Add(this);
            }
        }

        public void Detach()
        {
            if (parent != null)
            {
                parent.attachedMotions.Remove(this);
                parent = null;
            }
        }

        // 地面の法線（地面に立っていない時は真上を指します。）
        public Vector2 groundNormal { get; private set; }

        // C#のアクセサは計算によってプロパティを返す事ができます。
        // ここは、地面の法線に対して、「右方向」を返します。つまり、
        // 地面にそって右を移動した場合の移動方向です。
        public Vector2 groundRight
        {
            get
            {
                return new Vector2(groundNormal.y, -groundNormal.x);
            }
        }

        public Vector2 groundVelocity
        {
            get
            {
                if (groundMotion != null)
                {
                    return groundMotion.velocity;
                }
                else
                {
                    return Vector2.zero;
                }
            }
        }

        float groundDistance = 0f;

        new public Rigidbody2D rigidbody
        {
            get
            {
                if (rb == null)
                {
                    rb = GetComponent<Rigidbody2D>();
                }

                return rb;
            }
        }

        public Vector2 position
        {
            get
            {
                return rigidbody.position;
            }

            set
            {
                rigidbody.position = value;
            }
        }

        public Bounds GetBounds(bool updateColliders = false)
        {
            if (updateColliders) {
                colliders = GetComponentsInChildren<Collider2D>();
            }
            Bounds totalBounds = new Bounds();
            bool init = false;
            foreach(Collider2D coll in colliders)
            {
                if (!coll.isTrigger)
                {
                    if (init)
                    {
                        totalBounds.Encapsulate(coll.bounds);
                    }
                    else
                    {
                        totalBounds = coll.bounds;
                        init = true;
                    }
                }
            }
            
            return totalBounds;
        }

        public Bounds bounds
        {
            get
            {
                return GetBounds();
            }
        }

        protected virtual bool CanPush(KinematicMotion2D otherMotion, Vector2 delta)
        {
            if (otherMotion.groundMotion == this)
            {
                // こちらが地面なら衝突相手を押さない。必要な処理はGroundMovedで行われます。
                return false;
            }
            if (otherMotion.pushable == pushable)
            {
                return pushPriority > otherMotion.pushPriority;
            }
            else return otherMotion.pushable;
        }

        protected float GravityDirection
        {
            get
            {
                return Mathf.Sign(Physics2D.gravity.y) * Mathf.Sign(gravityModifier);
            }
        }

        public struct Contact
        {
            public GameObject self;
            public Rigidbody2D rigidbody;
            public Collider2D collider;
            public Vector2 point;
            public Vector2 normal;
            public Vector2 direction;
            public Vector2 relativeVelocity;
            public bool sliding;

            public bool Equals(Contact obj)
            {
                return obj.rigidbody == this.rigidbody && obj.collider == this.collider && obj.direction == this.direction;
            }

            public override bool Equals(System.Object obj)
            {
                return base.Equals(obj);
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public static bool operator ==(Contact c1, Contact c2)
            {
                return c1.Equals(c2);
            }

            public static bool operator !=(Contact c1, Contact c2)
            {
                return !c1.Equals(c2);
            }
        }

        // スクリプトで使うコンポーネントへの参照です。
        protected Rigidbody2D rb;

        // 衝突判定に必要な変数。
        // 効率をよくするために、メソッドの外で宣言しておきます。
        protected RaycastHit2D[] hits = new RaycastHit2D[8];
        protected RaycastHit2D[] colliderHits = new RaycastHit2D[8];
        protected Collider2D[] overlapColliders = new Collider2D[8];
        protected Collider2D[] overlaps = new Collider2D[8];
        protected ContactFilter2D contactFilter = new ContactFilter2D();

        protected Collider2D[] colliders;

        protected KinematicMotion2D groundMotion = null;

        // 追従（動く地面についていく処理）で使う地面の法線です。
        // 上の「groundNormal」は着地したフレームでは意図的に更新されません（斜面で滑るのを
        // 防ぐため。UpdateGroundを参照）。しかし、地面に乗った瞬間からその地面の動きに
        // 追従するので、追従用には着地フレームでも正しい法線が必要です。そのため別に持ちます。
        private Vector2 groundContactNormal = Vector2.up;

        // 追従中だけ、衝突判定から除外する相手です。
        // 地面が動く時、地面は「これから動く」という通知（WillMove）を、実際に動く前に
        // 出します。つまり追従の処理を行う時点で、地面はまだ古い位置にあります。その古い
        // 位置と衝突判定をしても意味がないので、追従の間だけ地面を無視します。
        // （地面が自分の乗客を無視するのと対称の処理です。Castを参照。）
        private KinematicMotion2D ignoredMotion = null;

        protected Vector2 positionAdjustment = Vector2.zero;

        protected virtual LayerMask GetCollisionMask()
        {
            return collisionMask;
        }


        protected virtual float ProcessCollision(RaycastHit2D hit, Vector2 direction, float distanceRemaining)
        {
            return distanceRemaining;
        }

        public void MoveBy(Vector2 delta)
        {
            Slide(delta);
        }


        // このメソッドはKinematicMotion2Dの肝心な処理を行います。
        // 「delta」パラメータで指定した距離までオブジェクトを移動します。
        // もし、途中で何かと衝突したら、衝突した面の向きによって、
        // 面に沿って移動を続けます。つまり、面に沿って「スライド」します。
        // 「iteration」パラメータは一回の移動でこれまでに何回スライドしたかを
        // 指定します。
        protected void Slide(Vector2 delta, int iterations = 0)
        {
            // まず、移動の方向と距離を抽出します。
            Vector2 direction = delta.normalized;
            float distance = delta.magnitude;

            // 小さすぎる動きを無視します。
            if (distance < minSlideDistance || distance == 0f)
            {
                return;
            }

            // 独自のメソッドで指定の場所まで移動したら何かに衝突するかを確かめます。
            RaycastHit2D hit; // 衝突があった場合、詳細がここで記憶されます。
            bool collided = Cast(direction, distance, out hit); // 衝突があったかが返されます。

            if (collided) // 衝突しました...
            {
                float contactDistance = hit.distance; // 衝突した位置までの距離

                // スライドできるようですので、まだ残っている移動距離を計算します。
                float distanceRemaining = distance - contactDistance;

                // 一応、衝突せずに移動できるところまで移動します。
                MoveRigidbody(direction * contactDistance);

                distanceRemaining = ProcessCollision(hit, direction, distanceRemaining);

                if (distanceRemaining == 0)
                {
                    return;
                }

                // スライドを試す回数がまだ残っているか？
                if (iterations < maxIterations)
                {
                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Kinematic)
                    {
                        KinematicMotion2D otherMotion = hit.rigidbody.gameObject.GetComponent<KinematicMotion2D>();
                        Vector2 remainingDelta = direction * distanceRemaining;

                        if (otherMotion != null && CanPush(otherMotion, remainingDelta))
                        {
                            float totalMass = mass + hit.rigidbody.mass;
                            float massRatio = totalMass > 0 ? mass / totalMass : 0f;
                            Vector2 startPosition = hit.rigidbody.position;
                            otherMotion.Slide(remainingDelta);
                            Vector2 pushDelta = hit.rigidbody.position - startPosition;
                            distanceRemaining = pushDelta.magnitude * massRatio;
                        }

                        Slide(direction * distanceRemaining, iterations + 1);
                        return;
                    }

                    if (hit.rigidbody != null && hit.rigidbody.bodyType == RigidbodyType2D.Dynamic)
                    {
                        // ダイナミックボディのコライダをキャストして、安全に移動できる距離を求めます。
                        int dynamicHitCount = hit.collider.Cast(direction, contactFilter, colliderHits, distanceRemaining + margin);
                        float totalMass = mass + hit.rigidbody.mass;
                        float massRatio = totalMass > 0 ? mass / totalMass : 0f;
                        float pushDistance = distanceRemaining * massRatio;
                        for (int j = 0; j < dynamicHitCount; j++)
                        {
                            // 自分自身のコライダへのヒットをスキップします（押している側のボディ）。
                            Collider2D hitCollider = colliderHits[j].collider;
                            bool isSelf = false;
                            foreach (Collider2D c in colliders)
                            {
                                if (c == hitCollider) { isSelf = true; break; }
                            }
                            if (isSelf) continue;

                            float d = colliderHits[j].distance - margin;
                            if (d < pushDistance)
                            {
                                pushDistance = d;
                            }
                        }
                        
                        pushDistance = Mathf.Max(0f, pushDistance);

                        // ダイナミックボディはMovePositionが次のフレームまで遅延されるため、
                        // 位置を直接書き換えて即座に同期的な移動を行います。
                        hit.rigidbody.position += direction * pushDistance;
                        Physics2D.SyncTransforms();

                        // 空いたスペースへのスライドを続けます。
                        Slide(direction * pushDistance, iterations + 1);
                        return;
                    }

                    // スライドができるのは地面と飛行中の天井だけです。
                    if (IsGroundNormal(hit.normal) || (IsCeilingNormal(hit.normal) && !isGrounded))
                    {
                        // 衝突した面に対する「右方向」
                        Vector2 right = new Vector2(Mathf.Abs(hit.normal.y), hit.normal.y < 0 ? hit.normal.x : -hit.normal.x);

                        // スライドするのは、横方向のみ。力学敵におかしいですが、落下からの着地した時に、
                        // 滑りを止めるための処理です。
                        Vector2 slideDelta = new Vector2(direction.x * distanceRemaining, 0);

                        // 数学でベクトルを習っていないと以下の行が少し難解ですが、
                        // 「ベクトル射影」を使って、目指す移動（slideDelta）を接触面の方向（right）に
                        // 修正した場合、どれくらいの移動（projection）になるかを計算します。
                        // ベクトル射影とその計算で行う「ベクトル内積（Dot Product）」はゲームでよく使う
                        // 演算ですので、覚えておくといいです。
                        //Vector2 projection = right * Vector2.Dot(right, slideDelta);
                        Vector2 projection = right * direction.x * distanceRemaining; // 移動距離の合計が変わらないようにします。

                        // ここは、今度、プログラミング初心者にとって特に理解が難しいテクニックを使います。
                        // あるメソッドが自身を呼び出すという「再帰的メソッド」または「再帰的関数」です。
                        // ここまでは、移動中に何かと衝突して、動けることろまで動きました。方向を変えて、
                        // 移動を続けたいですが、その途中でまた何かと衝突するかも知れません。
                        // その処理を行うために、もう一度最初からSlideを実行します。メソッドを再帰的に
                        // 呼び出すと無限に繰り返す危険があるので、必ず繰り返した回数を記憶して、ここみたいに
                        // 一定の回数以上繰り返さないチェックを入れます。
                        Slide(projection, iterations + 1);
                    }
                }
            }
            else
            {
                // 衝突がなかったので、オブジェクトの位置を変えます。
                // オブジェクトの位置を変える時に、通常の物理演算に悪影響がないように
                // transformではなく、Rigidbody2Dコンポーネント経由で動かします。
                MoveRigidbody(delta);
            }
        }

        protected int RigidbodyOverlap(ContactFilter2D contactFilter, Collider2D[] overlaps)
        {
            int totalHits = 0;
            foreach (Collider2D coll in colliders)
            {
                if (coll != null && !coll.isTrigger)
                {
                    int count = coll.Overlap(contactFilter, overlapColliders);
                    for (int i = 0; i < count; i++)
                    {
                        overlaps[totalHits] = overlapColliders[i];
                        totalHits++;
                        if (totalHits >= overlaps.Length)
                        {
                            return overlaps.Length;
                        }
                    }
                }
            }
            return totalHits;
        }

        protected int RigidbodyCast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] hits, float distance)
        {
            int totalHits = 0;
            foreach(Collider2D coll in colliders)
            {
                if (coll != null && !coll.isTrigger)
                {
                    int count = coll.Cast(direction, contactFilter, colliderHits, distance);
                    for (int i = 0; i < count; i++)
                    {
                        hits[totalHits] = colliderHits[i];
                        totalHits++;
                        if (totalHits >= hits.Length) {
                            return hits.Length;
                        }
                    }
                }
            }
            return totalHits;
        }

        // このコンポーネントでもう一つの重要なメソッドです。移動方向（direction）と距離（distance）に
        // 移動した場合、何かに衝突するかを確認します。衝突があったかどうかを返します。衝突があった場合、
        // hitにその詳細が記憶されます。
        public bool Cast(Vector2 direction, float distance, out RaycastHit2D hit)
        {
            distance += margin; // 移動距離に隙間を足します。
            bool collided = false; // 衝突したか？初期値はfalseに。

            hit = new RaycastHit2D(); // 衝突がなかった時にhitは初期のままにします。
            contactFilter.layerMask = GetCollisionMask(); // 衝突するとしないUnityのレイヤーを準備します。
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            // Rigidbody2Dの「Cast」メソッドで衝突判定を行います。「Cast」とは、直訳すると釣りの専門用語で
            // 「竿で仕掛けを飛ばす」という意味です。オブジェクトについているコライダが空間で指定と方向と距離で
            // 移動すれば、何に当たるかを計算する処理です。ゲームプログラミングの基本的な演算の一つです。
            int hitCount = RigidbodyCast(direction, contactFilter, hits, distance);

            // hitCountを当たったコライダの回数です。
            if (hitCount > 0) // 何かと衝突した...
            {
                // 衝突したコライダを一つずつ確認して、最も近い接触点を探します。
                for (int i = 0; i < hitCount; i++)
                {
                    // 既に見つけた接触より遠ければ確認する必要がありません。
                    if (hits[i].distance >= distance)
                    {
                        continue;
                    }

                    PlatformEffector2D effector = hits[i].collider.GetComponent<PlatformEffector2D>();
                    if (effector && effector.useOneWay) {
                        // 注意：surfaceArcはまだ使用していない
                       Quaternion angle = effector.transform.rotation * Quaternion.Euler(0, 0, effector.rotationalOffset);
                       float dot = Vector2.Dot(velocity, angle * Vector3.up);
                       if (dot > 0 || hits[i].distance < 0.0001f) {
                            continue;
                        }
                    }

                    KinematicMotion2D otherMotion = hits[i].collider.GetComponentInParent<KinematicMotion2D>();
                    if (otherMotion != null &&
                        // こちらが地面なら衝突として判定しない
                        (otherMotion.groundMotion == this ||
                        // 追従中の地面はまだ動いていないので衝突として判定しない
                         otherMotion == ignoredMotion))
                    {
                        continue;
                    }

                    // 注意：「distance」を更新するのは、無視しないと決めた接触だけです。
                    // 無視する接触で更新してしまうと、その奥にある本物の接触が
                    // 「もう見つけた接触より遠い」と判定されて見落とされてしまいます。
                    distance = hits[i].distance;

                    // 今まで確認した接触の中で最も近いですので、詳細を覚えておきます。
                    hit = hits[i]; // 衝突の詳細情報を記憶します。
                    hit.distance -= margin; // 移動距離からマージンを引いて、衝突からオブジェクトを離します。

                    collided = true; // 衝突したことを記憶します。
                }
            }

            return collided; // 衝突したかどうかを返します。
        }

        public static float maximumContactOffset
        {
            get
            {
                // 以下の調整はUnityの衝突判定の実装による誤差の補正です。Unityの2D物理演算は裏でオープンソースライブラリの「Box2D」を使用しています。
                // Box2Dに「b2_polygonRadius」という値があって衝突判定に使われています。
                // https://github.com/erincatto/Box2D/blob/ef96a4f17f1c5527d20993b586b400c2617d6ae1/Box2D/Common/b2Settings.h#L81
                // Unityでは、プロジェクト設定の「Default Contact Offset」でそのパラメータが調整できるそうです。
                // https://forum.unity.com/threads/what-is-default-contact-offset.750872/
                // しかし、2Dでは「Default Contact Offset」が無効のようで、変えてもBox2Dの「b2_polygonRadius」が使われるようです。
                // その値は「0.01f」ですので、ここでその半分を足して衝突のより正確な位置を求めます。

                return 0.005f;
            }
        }

        // このメソッドで渡された法線を持つ面が地面に該当するかどうかを返します。
        public bool IsGroundNormal(Vector2 normal)
        {
            // 法線と真上を指すベクトルの角度を計算して、しきい値より低いか返します。
            return Vector2.Angle(Vector2.down * GravityDirection, normal) < maxGroundAngleDegrees;
        }

        public bool IsWallNormal(Vector2 normal)
        {
            return !IsGroundNormal(normal) && !IsCeilingNormal(normal);
        }

        // このメソッドはスライドできる天井かどうかを返します。
        public bool IsCeilingNormal(Vector2 normal)
        {
            // 法線と真上を指すベクトルの角度を計算して、しきい値より低いか返します。
            return Vector2.Angle(Vector2.down, normal) < maxCeilingAngleDegrees;
        }

        protected virtual void Awake()
        {

        }

        // 初期の処理
        protected virtual void Start()
        {
            rb = GetComponent<Rigidbody2D>(); // Rigidbody2Dコンポーネントへの参照を取得

            // Rigidbody2Dの種類を「キネマティック」にします。そうすると、物理エンジンではなく、
            // ここのスクリプトがオブジェクトを動かします。
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;

            colliders = GetComponentsInChildren<Collider2D>();

            // このスクリプトは重力が真下へ働く前提で作られています。しかし、プロジェクト設定で、
            // どの方向にも重力を設定する事ができます。もし、このスクリプトと互換性のない設定が
            // 検出されたらエラーをコンソールに出力します。
            if (Physics2D.gravity.x != 0f || Physics2D.gravity.y > 0f)
            {
                Debug.LogError("重力が真下へ向かっていないとこのコンポーネントは正しく動作しません。プロジェクト設定を確認してください。");
            }

            groundNormal = Vector2.up;
            groundContactNormal = Vector2.up;
        }

        private Contact[][] contactBuffer = new Contact[][] {
            new Contact[16],
            new Contact[16]
        };

        private int[] contactCounts = new int[]
        {
            0, 0
        };

        private int contactIndex = 0;
        private int oldContactIndex = 1;

        private void SetContactIndex(int index)
        {
            if (index == 0)
            {
                contactIndex = 0;
                oldContactIndex = 1;
            }
            else
            {
                contactIndex = 1;
                oldContactIndex = 0;
            }
        }

        private void SwitchContactBuffers()
        {
            SetContactIndex(oldContactIndex);
        }

        protected Contact[] contacts => contactBuffer[contactIndex];
        protected Contact[] oldContacts => contactBuffer[oldContactIndex];
        protected int contactCount => contactCounts[contactIndex];
        protected int oldContactCount => contactCounts[oldContactIndex];

        void CheckForContacts(Vector2 direction)
        {
            if (contactCount < contacts.Length)
            {
                contactFilter.layerMask = GetCollisionMask(); // 衝突するとしないUnityのレイヤーを準備します。
                contactFilter.useLayerMask = true;
                contactFilter.useTriggers = false;

                int hitCount = RigidbodyCast(direction, contactFilter, hits, margin);
                int ndx = contactCount;
                for (int i = 0; i < hitCount && ndx < contacts.Length; i++, ndx++)
                {
                    contacts[ndx].self = gameObject;
                    contacts[ndx].rigidbody = hits[i].rigidbody;
                    contacts[ndx].collider = hits[i].collider;
                    contacts[ndx].normal = hits[i].normal;
                    contacts[ndx].point = hits[i].point;
                    contacts[ndx].direction = direction;

                    KinematicMotion2D km = hits[i].collider.gameObject.GetComponent<KinematicMotion2D>();
                    if (km != null)
                    {
                        contacts[ndx].relativeVelocity = velocity - km.velocity;
                    }
                    else
                    {
                        Rigidbody2D rb2d = hits[i].collider.gameObject.GetComponent<Rigidbody2D>();
                        if (rb2d != null)
                        {
                            contacts[ndx].relativeVelocity = velocity - rb2d.linearVelocity;
                        }
                        else
                        {
                            contacts[ndx].relativeVelocity = velocity;
                        }
                    }
                    contactCounts[contactIndex]++;
                }
            }
            
        }

        void UpdateContacts()
        {
            SwitchContactBuffers();

            contactCounts[contactIndex] = 0;

            CheckForContacts(Vector2.down);
            CheckForContacts(Vector2.right);
            CheckForContacts(Vector2.left);
            CheckForContacts(Vector2.up);

            // 新しい接触と継続中の接触
            for (int i = 0; i < contactCount; i++)
            {
                bool found = false;
                for (int j = 0; j < oldContactCount; j++)
                {
                    if (contacts[i] == oldContacts[j])
                    {
                        ContactStay(contacts[i]);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ContactEnter(contacts[i]);
                }
            }

            // 消えた接触
            for (int i = 0; i < oldContactCount; i++)
            {
                bool found = false;
                for (int j = 0; j < contactCount; j++)
                {
                    if (oldContacts[i] == contacts[j])
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    ContactExit(oldContacts[i]);
                }
            }

        }

        // 物体が「潰された」時に呼ばれます。
        //
        // 「潰された」とは、重なりがどうしても解消できない状態を指します。つまり、動いている
        // 何か（KinematicMotion2D、普通のキネマティックRigidbody2D、ダイナミックRigidbody2D
        // のいずれでも）に押し込まれて、逃げ場がない状態です。相手が何であるかはcontactの
        // colliderとrigidbodyで判別できます。
        //
        // 通知は「潰された瞬間」に一度だけ行われます（接地判定のjustLandedと同じ考え方です）。
        // 潰されている間ずっと処理を続けたい場合は、受け取った側で状態を保持してください。
        protected virtual void CrushedBy(Contact contact)
        {
            SendMessage("OnCrushedBy", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactEnter(Contact contact)
        {
            SendMessage("OnContactEnter", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactExit(Contact contact)
        {
            SendMessage("OnContactExit", contact, SendMessageOptions.DontRequireReceiver);
        }

        protected virtual void ContactStay(Contact contact)
        {
            SendMessage("OnContactStay", contact, SendMessageOptions.DontRequireReceiver);
        }

        private void MoveRigidbody(Vector2 delta)
        {
            WillMove?.Invoke(delta);
            
            rb.position += delta;

            // アタッチされたオブジェクトを動かす
            foreach (KinematicMotion2D attached in attachedMotions)
            {
                attached.rb.position += delta;
            }
        }

        // 追従の移動をどこまで小さければ無視するか。
        // Slideは「distance == 0f」という完全一致でしか早期終了しないため、
        // 1e-9のような誤差レベルの成分でも「margin」分のCastが走ってしまいます。
        // 面にぴったり接している物体はその時にhit.distance ≒ 0を返し、marginを引くと
        // 負の値になって、物体が逆方向へ跳ねてしまいます。それを防ぐためのしきい値です。
        private const float carryEpsilon = 1e-5f;

        // 乗っている地面が動く時に呼ばれます。地面の動き（delta）に追従します。
        private void GroundWillMove(Vector2 delta)
        {
            KinematicMotion2D ground = groundMotion;

            // 「sticky」な地面なら、どれだけ速く下降しても乗っている物体を引き連れます。
            // そうでない地面の場合は、縦方向の追従を行いません。自由落下より速く下降する
            // 地面は物体から離れていき、物体は重力に従って落下します。
            if (delta.y < 0f && ground != null && !ground.sticky)
            {
                delta.y = 0f;
            }

            // FixedUpdateで自分の移動を扱う時と同じように、地面の向きを基準にした
            // 「横」と「縦」に分けて移動します。こうすると、横の動きが障害物に
            // 止められても縦の動きは残るので、物体は地面の面に沿ったまま
            // （面から浮いたり沈んだりせずに）押されます。
            Vector2 normal = groundContactNormal;
            Vector2 right = new Vector2(normal.y, -normal.x);

            float alongGround = Vector2.Dot(right, delta);
            float acrossGround = Vector2.Dot(normal, delta);

            // このメソッドは地面が実際に動く「前」に呼ばれるので、地面はまだ古い位置に
            // あります。その古い位置と衝突しないように、追従の間だけ地面を無視します。
            // 「ignoredMotion」を元に戻すのは、追従の途中で例外が起きても、また追従が
            // 入れ子になっても正しく動くようにするためです。
            KinematicMotion2D previousIgnored = ignoredMotion;
            ignoredMotion = ground;
            try
            {
                if (Mathf.Abs(alongGround) > carryEpsilon)
                {
                    MoveBy(right * alongGround);
                }

                if (Mathf.Abs(acrossGround) > carryEpsilon)
                {
                    MoveBy(normal * acrossGround);
                }
            }
            finally
            {
                ignoredMotion = previousIgnored;
            }
        }

        private Action<Vector2> WillMove;

        protected virtual void FixedUpdate()
        {
            float deltaSeconds = Time.fixedDeltaTime;

            if (!simulatePhysics)
            {
                return;
            }

            ProcessOverlaps();

            // 地面に立っているかどうかの状態を更新します。
            UpdateGroundedState();

            // 地面から離れた時間を更新します。
            if (!isGrounded)
            {
                timeInAir += deltaSeconds;
            }
            else
            {
                timeInAir = 0f;
            }

            if (useGravity && (!isGrounded || groundDistance > margin)) // 重力の影響を受けるか？
            {
                // 重力の方向へ加速します。「Time.fixedDeltaTime」は
                // 前回FixedUpdateが実行された時から経過した時間を記憶しています。
                velocity += Physics2D.gravity * deltaSeconds * gravityMultiplier * gravityModifier;
            }

            // 最大速度を超えていないかを確認します。
            if (Mathf.Abs(velocity.x) > maxSpeedSide)
            {
                // スピード違反しているようで、最大速度に減速します。
                velocity.x = Mathf.Sign(velocity.x) * maxSpeedSide;
            }

            velocity.y = Mathf.Clamp(velocity.y, -Mathf.Abs(maxSpeedDown), maxSpeedUp);

           
            // この１フレームで移動する距離を計算します。
            Vector2 motion = velocity * deltaSeconds;


            // 移動する前の位置を記憶しておきます。
            Vector2 startPosition = rb.position;

            // 動きを滑らかにして、爽快な操作感を実現するための工夫として、横に移動してから
            // 縦に移動します。ここでいう「横」と「縦」は絶対の方向ではなく、地面の向きに
            // 対する方向です。地面に立っていなければ、真上と真横になります。
            // ここもベクトル射影が登場します。
            Vector2 horizontalMotion = groundRight * Vector2.Dot(groundRight, motion);
            Vector2 verticalMotion = groundNormal * Vector2.Dot(groundNormal, motion);

            positionAdjustment = Vector2.zero;

            // 横、そして縦に移動します。
            Slide(horizontalMotion);
            Slide(verticalMotion);

            // 衝突とスライドをしたかも知れません。実際の移動を計算します。
            Vector2 actualMotion = rb.position - startPosition - positionAdjustment;

            // 実際の移動から実際の速度を計算します。
            velocity = actualMotion / deltaSeconds;

            if (isGrounded)
            {
                // 地面に立っている時の速度を記憶しておきます。プレーヤーの空中移動の計算に必要な値です。
                lastGroundVelocity = velocity;
            }

            UpdateContacts();
        }


        // オブジェクトを動かす処理は物理演算に影響が出る可能性があるので、FixedUpdateで行います。
        // しかし、アニメーションの更新はUpdateと同期しているので、アニメーター関連の処理はここで
        // 行います。
        protected virtual void Update()
        {
        }

        protected virtual void OnDestroy()
        {
            if (parent != null)
            {
                Detach();
            }
        }

        private static void Separate(KinematicMotion2D objectToMove, Collider2D otherCollider)
        {
            // 重なりの原因がどちらのオブジェクトかを判断するために、相手の速度を取得します。
            Vector2 otherVelocity = Vector2.zero;
            KinematicMotion2D otherMotion = otherCollider.GetComponentInParent<KinematicMotion2D>();
            if (otherMotion != null)
            {
                otherVelocity = otherMotion.velocity;
            }
            else
            {
                Rigidbody2D otherRb = otherCollider.attachedRigidbody;
                if (otherRb != null)
                {
                    otherVelocity = otherRb.linearVelocity;
                }
            }

            foreach(Collider2D coll in objectToMove.colliders)
            {
                if (coll == otherCollider)
                continue;
                if (coll != null && !coll.isTrigger)
                {
                   ColliderDistance2D colliderDistance2D = coll.Distance(otherCollider);
                    if (colliderDistance2D.isOverlapped)
                    {
                        Vector2 delta = colliderDistance2D.normal * colliderDistance2D.distance;

                        // 重なりがobjectToMoveとotherColliderのどちらの動きによって生じたかを判定します。
                        // deltaはobjectToMoveを重なりから押し出すためのベクトルです。
                        // -deltaはobjectToMoveが重なりを引き起こした場合の移動方向です。
                        // objectToMoveが重なりへ向かっておらず、相手が向かっていた場合は分離をスキップします。
                        Vector2 overlapDir = delta.normalized;
                        float selfContribution = Vector2.Dot(objectToMove.velocity, -overlapDir);
                        float otherContribution = Vector2.Dot(otherVelocity, overlapDir);

                        if (selfContribution <= 0.01f && otherContribution > 0f)
                        {
                            // 重なりはobjectToMoveではなく、相手の動きによって引き起こされました。
                            continue;
                        }

                        // 分離によって新たな重なりが生じない場合のみ適用します。
                        Vector2 originalPosition = objectToMove.rb.position;
                        objectToMove.rb.position += delta;
                        Physics2D.SyncTransforms();

                        ContactFilter2D overlapFilter = new ContactFilter2D();
                        overlapFilter.layerMask = objectToMove.GetCollisionMask();
                        overlapFilter.useLayerMask = true;
                        overlapFilter.useTriggers = false;

                        bool causesNewOverlap = false;
                        int overlapCount = coll.Overlap(overlapFilter, objectToMove.overlapColliders);
                        for (int j = 0; j < overlapCount; j++)
                        {
                            if (objectToMove.overlapColliders[j] != otherCollider)
                            {
                                causesNewOverlap = true;
                                break;
                            }
                        }

                        if (causesNewOverlap)
                        {
                            // 元に戻します — 分離すると別のオブジェクトに押し込まれてしまいます。
                            objectToMove.rb.position = originalPosition;
                            Physics2D.SyncTransforms();
                        }
                    }
                }
            }
        }

        // 「潰された」と判定するまでに、解消できない重なりが続く必要のあるフレーム数。
        // ProcessOverlapsはmaxIterationsの制限があるため、深い重なりを一度に解消しきれない
        // 事があります。一時的なものを「潰された」と誤検知しないようにします。
        private const int crushPersistenceFrames = 2;

        // 解消できない重なりが続いたフレーム数。
        private int unresolvedOverlapFrames = 0;

        // 一度でも重なりのない状態になったか。
        // 「潰された」のか「最初から埋まった場所に置かれた」のかを区別するために使います。
        // 潰されるというのは「大丈夫だった状態から、動く物に押し込まれた」という変化です。
        // 一方、生成位置が悪い物体は最初から埋まったままで、その変化が存在しません。
        private bool didEverResolveOverlaps = false;

        // 同じ「潰された」状態で何度も通知しないためのフラグ。
        private bool crushReported = false;

        // 配置ミスの警告を何度も出さないためのフラグ（毎フレーム出すとログが埋まります）。
        private bool reportedBadPlacement = false;

        // これより浅い重なりは通常の接触として扱い、無視します。
        private float CrushPenetrationTolerance
        {
            get
            {
                return Mathf.Max(margin, maximumContactOffset);
            }
        }

        // 解消できずに残っている重なりのうち、最も深いものを探します。
        private bool FindUnresolvedOverlap(out Collider2D blockingCollider, out ColliderDistance2D deepest)
        {
            blockingCollider = null;
            deepest = new ColliderDistance2D();

            float worst = -CrushPenetrationTolerance;

            contactFilter.layerMask = GetCollisionMask();
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D other = overlaps[i];
                if (other == null)
                {
                    continue;
                }

                // 自分自身のコライダは対象外です。
                if (other.attachedRigidbody != null && other.attachedRigidbody == rb)
                {
                    continue;
                }

                // タイルマップはProcessOverlapsでも解消を試みないので、ここでも対象外にします。
                if (other.GetComponent<TilemapCollider2D>() != null)
                {
                    continue;
                }

                foreach (Collider2D coll in colliders)
                {
                    if (coll == null || coll.isTrigger || coll == other)
                    {
                        continue;
                    }

                    ColliderDistance2D colliderDistance2D = coll.Distance(other);
                    if (colliderDistance2D.isValid && colliderDistance2D.distance < worst)
                    {
                        worst = colliderDistance2D.distance;
                        deepest = colliderDistance2D;
                        blockingCollider = other;
                    }
                }
            }

            return blockingCollider != null;
        }

        private Contact BuildCrushContact(Collider2D blockingCollider, ColliderDistance2D separation)
        {
            Contact contact = new Contact();
            contact.self = gameObject;
            contact.collider = blockingCollider;
            contact.rigidbody = blockingCollider.attachedRigidbody;
            contact.point = separation.pointB;

            // ColliderDistance2Dの法線は自分から相手へ向きます。他の接触イベントに合わせて、
            // normalは「相手の面から自分へ向かう向き」、directionは「自分から相手への向き」に
            // 揃えます。
            contact.normal = -separation.normal;
            contact.direction = separation.normal;
            contact.sliding = false;

            Vector2 otherVelocity = Vector2.zero;
            KinematicMotion2D otherMotion = blockingCollider.GetComponentInParent<KinematicMotion2D>();
            if (otherMotion != null)
            {
                otherVelocity = otherMotion.velocity;
            }
            else if (contact.rigidbody != null)
            {
                otherVelocity = contact.rigidbody.linearVelocity;
            }
            contact.relativeVelocity = velocity - otherVelocity;

            return contact;
        }

        // ProcessOverlapsが重なりを解消しきれたかどうかを判定して、必要なら通知します。
        // 一番外側の呼び出しでのみ実行します。
        private void HandleUnresolvedOverlaps()
        {
            Collider2D blockingCollider;
            ColliderDistance2D separation;

            if (!FindUnresolvedOverlap(out blockingCollider, out separation))
            {
                // 重なりのない状態になりました。
                didEverResolveOverlaps = true;

                if (unresolvedOverlapFrames > 0)
                {
                    unresolvedOverlapFrames--;
                }

                if (unresolvedOverlapFrames == 0)
                {
                    crushReported = false;
                }

                return;
            }

            unresolvedOverlapFrames++;

            if (unresolvedOverlapFrames < crushPersistenceFrames)
            {
                return;
            }

            if (!didEverResolveOverlaps)
            {
                // 一度も重なりのない状態になっていません。つまり「潰された」のではなく、
                // 最初から抜け出せない場所に置かれています。これは配置のミスなので、
                // ゲームの処理としてではなく、警告として開発者に知らせます。
                if (!reportedBadPlacement)
                {
                    reportedBadPlacement = true;
                    Debug.LogWarning(
                        $"KinematicMotion2D「{name}」は重なりを解消できない場所に配置されています。" +
                        $"「{blockingCollider.name}」と{-separation.distance:F3}食い込んでいて、逃げ場がありません。" +
                        $"（位置：{rb.position}）生成位置を確認してください。",
                        this);
                }

                return;
            }

            if (!crushReported)
            {
                crushReported = true;
                CrushedBy(BuildCrushContact(blockingCollider, separation));
            }
        }

        protected void ProcessOverlaps(int iterations = 0)
        {
            if (iterations > maxIterations)
            {
                return;
            }

            contactFilter.layerMask = GetCollisionMask(); // 衝突するとしないUnityのレイヤーを準備します。
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            int hitCount = RigidbodyOverlap(contactFilter, overlaps);
            for (int i = 0; i < hitCount; i++)
            {
                KinematicMotion2D otherMotion = overlaps[i].GetComponentInParent<KinematicMotion2D>();

                if (otherMotion != null)
                {
                    if (otherMotion.pushPriority < pushPriority)
                    {
                        Separate(otherMotion, colliders[0]);
                    }
                    else
                    {
                        Separate(this, overlaps[i]);
                        ProcessOverlaps(iterations + 1);
                        break;
                    }
                }
                else
                {
                    // タイルマップコライダとの重なりは正しく処理するのが難しいため、今のところスキップします（暫定対応）。
                    TilemapCollider2D tilemapCollider = overlaps[i].GetComponent<TilemapCollider2D>();
                    if (tilemapCollider == null)
                    {
                        Separate(this, overlaps[i]);
                        ProcessOverlaps(iterations + 1);
                        break;
                    }

                }
            }

            // ここまでで重なりの解消を試みました。結果を確認するのは一番外側の呼び出しだけです。
            // 「解消しようとした結果どうなったか」で判定するので、Separateの中身がどの分岐を
            // 通ったかに依存しません。
            if (iterations == 0)
            {
                HandleUnresolvedOverlaps();
            }
        }

        // 地面を検出します。指定の方向と距離に地面に当たるコライダを見つければ、trueを返します。
        // 衝突の詳細はhitに記憶されます。
        public bool CheckForGround(Vector2 direction, float distance, out RaycastHit2D hit)
        {
            hit = new RaycastHit2D(); // デフォルトに初期化する

            if (!useGravity)
            {
                return false;
            }

            contactFilter.layerMask = GetCollisionMask(); // 衝突するとしないUnityのレイヤーを準備します。
            contactFilter.useLayerMask = true;
            contactFilter.useTriggers = false;

            // Rigidbody2Dにキャストしてもらいます。
            int hitCount = RigidbodyCast(direction, contactFilter, hits, distance + margin);
            for (int i = 0; i < hitCount; i++)
            {
                // 重力の方向と衝突した面の法線を比較します。
                if (IsGroundNormal(hits[i].normal))
                {
                    // 地面を見つけたので詳細を記憶します。
                    hit = hits[i];
                    hit.distance -= margin;
                    return true; // これ以上地面を探す必要がありません。
                }
            }

            // ここまできたら地面は検出できませんでした。
            return false;
        }

        public void LateUpdate()
        {
            
        }

        protected virtual void Landed(float speed)
        {

        }

        protected virtual void Fell()
        {

        }

        void SetGroundMotion(KinematicMotion2D motion)
        {
            if (motion != groundMotion)
            {
                if (groundMotion != null)
                {
                    groundMotion.WillMove -= GroundWillMove;
                }

                groundMotion = motion;

                if (groundMotion != null)
                {
                    groundMotion.WillMove += GroundWillMove;
                }
            }
        }

        void UpdateGround(bool oldState, RaycastHit2D groundHit)
        {
            if (isGrounded)
            {
                // 追従用の法線は着地フレームでも更新します。地面に乗った瞬間から
                // その地面の動きに追従するので、その時点で正しい向きが必要です。
                groundContactNormal = groundHit.normal;

                // 地面に立っているので地面の向きを覚えておきます。
                if (oldState)
                {
                    // 着地した次のフレームから地面の法線を更新します。
                    // 着地した瞬間に更新すると、斜面で滑ってしまう問題があるためです。
                    groundNormal = groundHit.normal;
                    groundDistance = groundHit.distance;
                }

                // ここで、KinematicMotion2Dコンポーネントを持っているオブジェクトの上に立っているかどうかを確認します。
                // 立っているなら、その動きの影響を受けます。
                if (useGroundMotion)
                {
                    SetGroundMotion(groundHit.collider.gameObject.GetComponentInParent<KinematicMotion2D>());
                }

                // ここで少し細かい処理を行います。地面に立っていても上へ移動していれば、「地面に立っていない」と
                // 判定します。そうしないと、斜面上でジャンプをした時に、移動が横にそれてしまうからです。
                // しかし、斜面を登る時に、縦の速度が0より大きいので、斜面を登る移動とジャンプ・発射移動を識別するために、
                // 地面の向きと移動の向きを比較する必要があります。
                if (velocity.magnitude > 0.01f)
                {
                    // 地面から離れる方向に移動しているか？
                    if (Vector2.Dot(groundHit.normal, velocity.normalized) > 0.25f)
                    {
                        // これから「離陸」するので、地面に立っていないことにします。
                        // 地面から離れようとしているので、isGroundedをfalseにします。しかし、
                        // 地面が動いているなら、その影響を受けたいので、groundMotionは記憶した
                        // ままにします。（そうしないと、上昇している物体の上に立ってジャンプ
                        // しようとするとジャンプが正しく動作しません。
                        isGrounded = false;
                    }
                }
            }
            else
            {
                groundContactNormal = Vector2.up;
                SetGroundMotion(null);
            }

            justLanded = isGrounded && !oldState;
            justFell = !isGrounded && oldState;

            if (justLanded)
            {
                velocity -= groundVelocity;
                Landed(velocity.y);
            }

            if (justFell)
            {
                Fell();
            }
        }

        // このメソッドはオブジェクトが地面に立っているかどうか、と地面の向きの状態を更新します。
        public void UpdateGroundedState()
        {
            bool oldState = isGrounded;

            // 最初から地面に立っていないと仮定します。
            isGrounded = false;
            groundNormal = Vector2.up;
            groundDistance = 0f;

            // 地面を検出します。
            RaycastHit2D groundHit = new RaycastHit2D();
            isGrounded = CheckForGround(Vector2.up * GravityDirection, groundCheckDistance, out groundHit);

            UpdateGround(oldState, groundHit);
        }
    }
} // namespace

